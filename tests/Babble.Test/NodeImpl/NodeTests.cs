using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Babble.Core.Crypto;
using Babble.Core.HashgraphImpl.Model;
using Babble.Core.HashgraphImpl.Stores;
using Babble.Core.NetImpl;
using Babble.Core.NetImpl.PeerImpl;
using Babble.Core.NetImpl.TransportImpl;
using Babble.Core.NodeImpl;
using Babble.Core.ProxyImpl;
using Babble.Core.Util;
using Babble.Test.Helpers;
using Serilog;
using Xunit;
using Xunit.Abstractions;

namespace Babble.Test.NodeImpl
{
    public class NodeTests
    {
        private readonly ITestOutputHelper output;
        private readonly ILogger logger;
   
        public NodeTests(ITestOutputHelper output)
        {
            this.output = output;
            logger = output.SetupLogging().ForContext("SourceContext", "NodeTests");
            
        }

        private string GetPath() => $"localdb/{Guid.NewGuid():D}";

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

            var id0 = pmap[peers[0].PubKeyHex];
            var peer0Trans = await router.Register(peers[0].NetAddr);
      

            var node0 = new Node(config,id0 , keys[0], peers, new InmemStore(pmap, config.CacheSize, logger), peer0Trans, new InMemAppProxy(id0,logger), logger);
            await node0.Init(false);

            await node0.StartAsync(false);

            var id1 = pmap[peers[1].PubKeyHex];
            var peer1Trans = await router.Register(peers[1].NetAddr);
            var node1 = new Node(config,id1 , keys[1], peers, new InmemStore(pmap, config.CacheSize, logger), peer1Trans, new InMemAppProxy(id1,logger), logger);
            await node1.Init(false);

            await node1.StartAsync(false);

            //Manually prepare SyncRequest and expected SyncResponse

            var node0Known = await node0.Controller.KnownEvents();

            var node1Known = await node1.Controller.KnownEvents();

            Exception err;

            Event[] unknown;
            (unknown, err) = await node1.Controller.EventDiff(node0Known);
            Assert.Null(err);

            WireEvent[] unknownWire;
            (unknownWire, err) = node1.Controller.ToWire(unknown);
            Assert.Null(err);

            var args = new SyncRequest
            {
                FromId = node0.Id,
                Known = node0Known
            };

            var expectedResp = new SyncResponse
            {
                FromId = node1.Id,
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
            
            var id0 = pmap[peers[0].PubKeyHex];
            var peer0Trans = await router.Register(peers[0].NetAddr);
            var node0 = new Node(config,id0, keys[0], peers, new InmemStore(pmap, config.CacheSize, logger), peer0Trans, new InMemAppProxy(id0,logger), logger);
            await node0.Init(false);

            await node0.StartAsync(false);

            var peer1Trans = await router.Register(peers[1].NetAddr);

            var id1 = pmap[peers[1].PubKeyHex];
            var node1 = new Node(config, id1, keys[1], peers, new InmemStore(pmap, config.CacheSize, logger), peer1Trans, new InMemAppProxy(id1,logger), logger);
            await node1.Init(false);

            await node1.StartAsync(false);

            //Manually prepare EagerSyncRequest and expected EagerSyncResponse

            var node1Known = await node1.Controller.KnownEvents();

            Event[] unknown;
            Exception err;
            (unknown, err) = await node0.Controller.EventDiff(node1Known);
            Assert.Null(err);

            WireEvent[] unknownWire;
            (unknownWire, err) = node0.Controller.ToWire(unknown);
            Assert.Null(err);

            var args = new EagerSyncRequest
            {
                FromId = node0.Id,
                Events = unknownWire
            };

            var expectedResp = new EagerSyncResponse
            {
                FromId = node1.Id,
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

            var id0 = pmap[peers[0].PubKeyHex];
            var peer0Trans = await router.Register(peers[0].NetAddr);
            var peer0Proxy = new InMemAppProxy(id0,logger);
            var node0 = new Node(config, id0, keys[0], peers, new InmemStore(pmap, config.CacheSize, logger), peer0Trans, peer0Proxy, logger);
            await node0.Init(false);

            await node0.StartAsync(false);

            var id1 = pmap[peers[1].PubKeyHex];
            var peer1Trans = await router.Register(peers[1].NetAddr);
            var peer1Proxy = new InMemAppProxy(id1,logger);
            var node1 = new Node(config, id1, keys[1], peers, new InmemStore(pmap, config.CacheSize, logger), peer1Trans, peer1Proxy, logger);
            await node1.Init(false);

            await node1.StartAsync(false);

            //Submit a Tx to node0

            var message = "Hello World!";
            await peer0Proxy.SubmitTx(message.StringToBytes());
            await node0.AddingTransactionsCompleted();
            
            //simulate a SyncRequest from node0 to node1

            var node0Known = await node0.Controller.KnownEvents();
            var args = new SyncRequest
            {
                FromId = node0.Id,
                Known = node0Known
            };

            Exception err;
            SyncResponse resp;

            (resp, err) = await peer0Trans.Sync(peers[1].NetAddr, args);
            Assert.Null(err);

            err = await node0.Sync(resp.Events);
            Assert.Null(err);

            ////check the Tx was removed from the transactionPool and added to the new Head
            Assert.Empty(node0.Controller.TransactionPool);

            var (node0Head, _) = await node0.Controller.GetHead();
            Assert.Single(node0Head.Transactions());

            Assert.Equal(message, node0Head.Transactions()[0].BytesToString());

            node0.Shutdown();
            node1.Shutdown();
        }

        private static async Task<(CngKey[] keys, Node[] nodes)> InitNodes(int n, int cacheSize, int syncLimit, string storeType, string dbPath, ILogger logger)
        {
            var (keys, peers, pmap) = InitPeers(n);

            var nodes = new List<Node>();

            var proxies = new List<InMemAppProxy>();

            var router = new InMemRouter();

            for (var i = 0; i < peers.Length; i++)
            {
                var conf = new Config(TimeSpan.FromMilliseconds(5), TimeSpan.FromSeconds(1), cacheSize, syncLimit, storeType, $"{dbPath}/db_{i}");

                var trans = await router.Register(peers[i].NetAddr);

                IStore store = null;
                Exception err;
                switch (storeType)
                {
                    case "badger":
                        (store, err) = await LocalDbStore.New(pmap, conf.CacheSize, conf.StorePath, logger);
                        Assert.Null(err);
                        break;
                    case "inmem":
                        store = new InmemStore(pmap, conf.CacheSize, logger);
                        break;
                    default:
                        throw new NotImplementedException();
                }

                var id = pmap[peers[i].PubKeyHex];
                var proxy = new InMemAppProxy(id, logger);
                var node = new Node(conf,id, keys[i], peers,
                    store,
                    trans,
                    proxy, logger);

                err = await node.Init(false);

                Assert.Null(err);

                nodes.Add(node);
                proxies.Add(proxy);
            }

            return (keys.ToArray(), nodes.ToArray());
        }

        private static async Task<Node[]> RecycleNodes(Node[] oldNodes, ILogger logger)
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
            var key = oldNode.Controller.Key;
            var peers = oldNode.PeerSelector.Peers();

            IStore store = null;
            if (oldNode.Store is InmemStore)
            {
                store = new InmemStore(oldNode.Store.Participants().participants.Clone(), conf.CacheSize, logger);
            }

            if (oldNode.Store is LocalDbStore)
            {
                (store, _) = await LocalDbStore.Load(conf.CacheSize, conf.StorePath, logger);
            }

            Assert.NotNull(store);

            await oldNode.Trans.CloseAsync();

            var trans = await ((InMemRouterTransport) oldNode.Trans).Router.Register(oldNode.LocalAddr);

            var prox = new InMemAppProxy(id, logger);

            var newNode = new Node(conf, id, key, peers, store, trans, prox, logger);

            var err = await newNode.Init(true);
            Assert.Null(err);

            return newNode;
        }

        private static async Task RunNodes(Node[] nodes, bool gossip)
        {
            foreach (var n in nodes)
            {
                await n.StartAsync(gossip);
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
            var (keys, nodes) = await InitNodes(2, 1000, 1000, "inmem", GetPath(), logger);

            var err = await Gossip(nodes, 2, true, TimeSpan.FromSeconds(2),logger);
            Assert.Null(err);

            await CheckGossip(nodes, logger);

            ShutdownNodes(nodes);


        }

        [Fact]
        public async Task TestMissingNodeGossip()
        {
            var (keys, nodes) = await InitNodes(4, 1000, 1000, "inmem", GetPath(), logger);
            try
            {
                var err = await Gossip(nodes.Skip(1).ToArray(), 10, true, TimeSpan.FromSeconds(3),logger);
                Assert.Null(err);
                await CheckGossip(nodes.Skip(1).ToArray(), logger);
            }
            finally
            {
                ShutdownNodes(nodes);
            }
        }

        [Fact]
        public async Task TestSyncLimit()
        {
            var ( _, nodes) = await InitNodes(4, 1000, 300, "inmem", GetPath(), logger);

            var err = await Gossip(nodes, 10, false, TimeSpan.FromSeconds(3),logger);
            Assert.Null(err);

            try
            {
                //create fake node[0] known to artificially reach SyncLimit
                var node0Known = await nodes[0].Controller.KnownEvents();
                int k = 0;
                foreach (var kn in node0Known.ToList())
                {
                    node0Known[k] = 0;
                    k++;
                }

                var args = new SyncRequest
                {
                    FromId = nodes[0].Id,
                    Known = node0Known
                };

                var expectedResp = new SyncResponse
                {
                    FromId = nodes[1].Id,
                    SyncLimit = true
                };

                SyncResponse resp;
                (resp, err) = await nodes[0].Trans.Sync(nodes[1].LocalAddr, args);
                Assert.Null(err);

                // Verify the response

                Assert.Equal(expectedResp.FromId, resp.FromId);
                Assert.True(expectedResp.SyncLimit);
            }
            finally
            {
                ShutdownNodes(nodes);
            }
        }

        [Fact]
        public async Task TestShutdown()
        {
            var (_, nodes) = await InitNodes(2, 1000, 1000, "inmem",GetPath(), logger);

            await RunNodes(nodes, false);

            nodes[0].Shutdown();

            var err = await nodes[1].Gossip(nodes[0].LocalAddr);
            Assert.NotNull(err);

            nodes[1].Shutdown();
        }

        [Fact]
        public async Task TestBootstrapAllNodes()
        {
            //create a first network with BadgerStore and wait till it reaches 10 consensus
            //rounds before shutting it down
            var (_, nodes) = await InitNodes(4, 10000, 1000, "badger", GetPath(), logger);
            var err = await Gossip(nodes, 10, false, TimeSpan.FromSeconds(3),logger);
            Assert.Null(err);

            await CheckGossip(nodes, logger);
            ShutdownNodes(nodes);

            ////Now try to recreate a network from the databases created in the first step
            ////and advance it to 20 consensus rounds
            //var newNodes = await RecycleNodes(nodes, logger);
            //err = await Gossip(newNodes, 20, false, TimeSpan.FromSeconds(3));
            //Assert.Null(err);

            //await CheckGossip(newNodes, logger);
            //ShutdownNodes(newNodes);

            ////Check that both networks did not have completely different consensus events
            //await CheckGossip(new[] {nodes[0], newNodes[0]}, logger);
        }

        private static async Task<Exception> Gossip(Node[] nodes, int target, bool shutdown, TimeSpan timeout,ILogger logger)
        {
            await RunNodes(nodes, true);

            var err = await BombardAndWait(nodes, target, timeout, logger);
            Assert.Null(err);

            if (shutdown)
            {
                ShutdownNodes(nodes);
            }

            return null;
        }

        private static async Task<Exception> BombardAndWait(Node[] nodes, int target, TimeSpan timeout, ILogger logger)
        {
            var cts = new CancellationTokenSource();
            
            var mrtTask = MakeRandomTransactions(nodes, cts.Token);


            //wait until all nodes have at least 'target' rounds
           var stopper= Task.Delay(timeout, cts.Token);

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
                            var ce = n.Controller.GetLastConsensusRoundIndex();

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

          var resultTask= await Task.WhenAny( mrtTask,stopper, Bombard());

            if (resultTask == stopper)
            {
                return new Exception("timeout");
            }

            await resultTask;



            return null;
        }

        private static async Task CheckGossip(Node[] nodes, ILogger logger)
        {
            var consEvents = new Dictionary<int, string[]>();
            var consTransactions = new Dictionary<int, byte[][]>();
            foreach (var n in nodes)
            {
                //logger.Debug(n.Id.ToString());

                consEvents[n.Id] = n.Controller.GetConsensusEvents();

                var (nodeTxs, err) = await GetCommittedTransactions(n);
                Assert.Null(err);
                
                consTransactions[n.Id] = nodeTxs;
            }

            var minE = consEvents.ContainsKey(0) ? consEvents[0].Length : 0;
            var minT = consTransactions.ContainsKey(0) ? consTransactions[0].Length : 0;

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
            foreach (var e in consEvents.ContainsKey(0) ? consEvents[0] : new string[] { }.Take(minE))
            {
                int j = 0;
                foreach (var jn in nodes.Skip(1))
                {
                    var f = consEvents[j][i];
                    if (f != e)
                    {
                        var er = await nodes[0].Controller.Hg.Round(e);

                        var err = await nodes[0].Controller.Hg.RoundReceived(e);

                        var fr = await nodes[j].Controller.Hg.Round(f);

                        var frr = await nodes[j].Controller.Hg.RoundReceived(f);

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
            foreach (var tx in consTransactions.ContainsKey(0) ? consTransactions[0] : new byte[][] { }.Take(minT))
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
            var seq = new int[nodes.Length];
            while (!ct.IsCancellationRequested)
            {
                var rnd = new Random();
                var n = rnd.Next(0, nodes.Length);

                var node = nodes[n];

                var prox = node.Proxy as InMemAppProxy;
                Assert.NotNull(prox);

                await prox.SubmitTx( $"node{n} transaction {seq[n]}".StringToBytes());
                
                seq[n] = seq[n] + 1;

                await Task.Delay(3, ct);
            }
        }

        private static async Task SubmitTransaction(Node n, byte[] tx)
        {
          
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