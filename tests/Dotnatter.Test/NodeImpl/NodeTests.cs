using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Dotnatter.Crypto;
using Dotnatter.NetImpl;
using Dotnatter.NetImpl.PeerImpl;
using Dotnatter.NodeImpl;
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
        public void TestProcessSync()
        {
            var (keys, peers, pmap) = InitPeers(2);

            var config = Config.TestConfig();

            //Start two nodes

//	var peer0Trans, err := net.NewTCPTransport(peers[0].NetAddr, nil, 2, time.Second, testLogger)
//	if err != nil {
//		t.Fatalf("err: %v", err)
//	}
//	defer peer0Trans.Close()


//    node0:= NewNode(config, pmap[peers[0].PubKeyHex], keys[0], peers,
//       hg.NewInmemStore(pmap, config.CacheSize),
//       peer0Trans,
//       aproxy.NewInmemAppProxy(testLogger))

//    node0.Init(false)


//    node0.RunAsync(false)


//    peer1Trans, err:= net.NewTCPTransport(peers[1].NetAddr, nil, 2, time.Second, testLogger)

//    if err != nil {
//                t.Fatalf("err: %v", err)

//    }
//            defer peer1Trans.Close()


//    node1:= NewNode(config, pmap[peers[1].PubKeyHex], keys[1], peers,
//       hg.NewInmemStore(pmap, config.CacheSize),
//       peer1Trans,
//       aproxy.NewInmemAppProxy(testLogger))

//    node1.Init(false)


//    node1.RunAsync(false)

//    //Manually prepare SyncRequest and expected SyncResponse

//            node0Known:= node0.core.Known()

//    node1Known:= node1.core.Known()


//    unknown, err:= node1.core.Diff(node0Known)

//    if err != nil {
//                t.Fatal(err)

//    }

//            unknownWire, err:= node1.core.ToWire(unknown)

//    if err != nil {
//                t.Fatal(err)

//    }

//            args:= net.SyncRequest{
//                From: node0.localAddr,
//		Known: node0Known,
//	}
//            expectedResp:= net.SyncResponse{
//                From: node1.localAddr,
//		Events: unknownWire,
//		Known: node1Known,
//	}

//            //Make actual SyncRequest and check SyncResponse

//            var out net.SyncResponse

//    if err := peer0Trans.Sync(peers[1].NetAddr, &args, &out); err != nil {
//                t.Fatalf("err: %v", err)

//    }

//            // Verify the response
//            if expectedResp.From != out.From {
//                t.Fatalf("SyncResponse.From should be %s, not %s", expectedResp.From, out.From)

//    }

//            if l := len(out.Events); l != len(expectedResp.Events) {
//                t.Fatalf("SyncResponse.Events should contain %d items, not %d",
//                    len(expectedResp.Events), l)

//    }

//            for i, e := range expectedResp.Events {
//                ex:= out.Events[i]

//        if !reflect.DeepEqual(e.Body, ex.Body) {
//                    t.Fatalf("SyncResponse.Events[%d] should be %v, not %v", i, e.Body,
//                        ex.Body)

//        }
//            }

//            if !reflect.DeepEqual(expectedResp.Known, out.Known) {
//                t.Fatalf("SyncResponse.Known should be %#v, not %#v", expectedResp.Known, out.Known)

//    }

//            node0.Shutdown()

//    node1.Shutdown()
//}









        }


    }
}
