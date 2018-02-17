using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dotnatter.Crypto;
using Dotnatter.HashgraphImpl;
using Dotnatter.HashgraphImpl.Model;
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


        const int PortStart = 9990;

        private (CngKey[] keys, Peer[] peers, Dictionary<string,int> pmap) InitPeers(int n)
        {
            var port = PortStart;
            var keys = new List<CngKey> { };
            var peers = new List<Peer> { };

            int i = 0;
            for (i = 0; i < n; i++)
            {
                var key = CryptoUtils.GenerateEcdsaKey();
                keys.Add(key);
                peers.Add(new Peer
                (
                    netAddr: $"127.0.0.1:{port}",
                    pubKeyHex: CryptoUtils.FromEcdsaPub(key).ToHex()
                ));
                port++;
            }

            peers.Sort((peer, peer1) =>String.Compare(peer.PubKeyHex, peer1.PubKeyHex, StringComparison.Ordinal) );
            var pmap = new Dictionary<string,int>();

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

            var cts = new CancellationTokenSource();

            var peer0Trans = new InMemTransport(peers[0].NetAddr);
            var node0 = new Node(config, pmap[peers[0].PubKeyHex], keys[0], peers, new InmemStore(pmap,config.CacheSize,logger),peer0Trans, new InMemAppProxy(logger),logger  );
            node0.Init(false);

            var node0Task = node0.RunAsync(false, cts.Token);

            

            var peer1Trans = new InMemTransport(peers[1].NetAddr);

            await peer1Trans.ConnectAsync(peers[0].NetAddr, peer0Trans);
            await peer0Trans.ConnectAsync(peers[1].NetAddr, peer1Trans);
            
            var node1 = new Node(config, pmap[peers[1].PubKeyHex], keys[1], peers, new InmemStore(pmap,config.CacheSize,logger),peer1Trans, new InMemAppProxy(logger),logger  );
            node1.Init(false);

            var node1Task =node1.RunAsync(false, cts.Token);



            //Manually prepare SyncRequest and expected SyncResponse

            var node0Known = node0.Core.Known();

            var node1Known = node1.Core.Known();
            
            Exception err;
            
            Event[] unknown;
            (unknown, err) = node1.Core.Diff(node0Known);
            Assert.Null(err);

            WireEvent[] unknownWire;
            (unknownWire, err) = node1.Core.ToWire(unknown);
            Assert.Null(err);

            var args = new SyncRequest
            {
                From = node0.LocalAddr,
                Known = node0Known,
            };

            var expectedResp = new SyncResponse
            {
                From = node1.LocalAddr,
                Events = unknownWire,
                Known = node1Known,
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
    }
}
