using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Babble.Core;
using Babble.Core.Crypto;
using Babble.Core.HashgraphImpl.Model;
using Babble.Core.HashgraphImpl.Stores;
using Babble.Core.PeersImpl;
using Babble.Core.Util;
using Babble.Test.Helpers;
using Serilog;
using Xunit;
using Xunit.Abstractions;

namespace Babble.Test.HashgraphImpl
{
    public class InmemStoreTests
    {
        private ILogger logger;

        public InmemStoreTests(ITestOutputHelper output)
        {
            logger = output.SetupLogging();
        }

        public async Task<(InmemStore, Pub[])> InitInmemStore(int cacheSize)
        {
            var n = 3;
            var participantPubs = new List<Pub>();
            var participants = Peers.NewPeers();

            for (var i = 0; i < n; i++)
            {
                var key = CryptoUtils.GenerateEcdsaKey();
                var pubKey = CryptoUtils.FromEcdsaPub(key);
                var peer = Peer.New(pubKey.ToHex(), "");

                participantPubs.Add(new Pub {Id = i, PrivKey = key, PubKey = pubKey, Hex =peer.PubKeyHex});
                await participants.AddPeer(peer);

                participantPubs[participantPubs.Count - 1].Id = peer.ID;

            }

            var store =await InmemStore.NewInmemStore(participants, cacheSize, Log.Logger);
            return (store, participantPubs.ToArray());
        }

        [Fact]
        public async Task TestInmemEvents()
        {
            var cacheSize = 100;

            var testSize = 15;

            var (store, participants) =await  InitInmemStore(cacheSize);

            var events = new Dictionary<string, List<Event>>();

            // Store Events
            foreach (var p in participants)
            {
                var items = new List<Event>();

                for (var k = 0; k < testSize; k++)
                {
                    var ev = new Event(new[] {$"{p.Hex}_{k}".StringToBytes()}, new[] {new BlockSignature {Validator = "validator".StringToBytes(), Index = 0, Signature = "r|s".StringToBytes()}},
                        new[] {"", ""},
                        p.PubKey,
                        k);

                    items.Add(ev);
                    await store.SetEvent(ev);
                }

                events[p.Hex] = items;
            }

            foreach (var evi in events)
            {
                foreach (var ev in evi.Value)
                {
                    var (rev, err) = await store.GetEvent(ev.Hex());

                    Assert.Null(err);
                    rev.Body.ShouldCompareTo(ev.Body);
                }
            }

            // Check ParticipantEventsCache
            var skipIndex = -1; //do not skip any indexes
            foreach (var p in participants)
            {
                var (pEvents, err) = await store.ParticipantEvents(p.Hex, skipIndex);

                Assert.Null(err);

                var l = pEvents.Length;

                Assert.Equal(testSize, l);

                var expectedEvents = events[p.Hex].Skip(skipIndex + 1).ToArray();
                foreach (var e in expectedEvents)
                {
                    var k = e.Index();
                    Assert.Equal(e.Hex(), pEvents[k]);
                }
            }

            // Check ConsensusEvents
            var expectedKnown = new Dictionary<int, int>();

            foreach (var p in participants)
            {
                expectedKnown.Add(p.Id, testSize - 1);
            }

            var known = await store.KnownEvents();

            known.OrderBy(o=>o.Key).ToList().ShouldCompareTo(expectedKnown.OrderBy(o=>o.Key).ToList());

            // Add ConsensusEvents
            foreach (var p in participants)
            {
                var evs = events[p.Hex];
                foreach (var ev in evs)

                {
                  var err=  store.AddConsensusEvent(ev);
                    Assert.Null(err);
                }
            }
        }

        [Fact]
        public async Task TestInmemRounds()
        {
            var ( store, participants) = await  InitInmemStore(10);

            var round = new RoundInfo();

            var events = new Dictionary<string, Event>();

            foreach (var p in participants)
            {
                var ev = new Event(new[] {new byte[] { }}, new BlockSignature[] { },
                    new[] {"", ""},
                    p.PubKey,
                    0);
                events[p.Hex] = ev;
                round.AddEvent(ev.Hex(), true);
            }

            // Store Round
            var err = await store.SetRound(0, round);
            Assert.Null(err);

            RoundInfo storedRound;
            (storedRound, err) = await store.GetRound(0);
            Assert.Null(err);

            round.ShouldCompareTo(storedRound);

            // Check LastRound

            var c = store.LastRound();
            Assert.Equal(0, c);

            // Check witnesses

            var witnesses = await store.RoundWitnesses(0);
            var expectedWitnesses = round.Witnesses();

            Assert.Equal(expectedWitnesses.Length, witnesses.Length);

            foreach (var w in expectedWitnesses)
            {
                Assert.Contains(w, witnesses);
            }
        }

        [Fact]
        public async Task TestInmemBlocks()
        {
            var (store, participants) = await InitInmemStore(10);

            var index = 0;
            var roundReceived = 7;
            var transactions = new[]
            {
                "tx1".StringToBytes(),
                "tx2".StringToBytes(),
                "tx3".StringToBytes(),
                "tx4".StringToBytes(),
                "tx5".StringToBytes()
            };
            var frameHash = "this is the frame hash".StringToBytes();
            var block = new Block(index, roundReceived, frameHash, transactions);

            BabbleError err;
            BlockSignature sig1;

            (sig1, err) = block.Sign(participants[0].PrivKey);
            Assert.Null(err);

            BlockSignature sig2;
            (sig2, err) = block.Sign(participants[1].PrivKey);
            Assert.Null(err);

            block.SetSignature(sig1);
            block.SetSignature(sig2);

            // Store Block

            err = await store.SetBlock(block);
            Assert.Null(err);

            Block storedBlock;
            (storedBlock, err) = await store.GetBlock(index);
            Assert.Null(err);

            storedBlock.ShouldCompareTo(block);

            // Check signatures in stored Block

            (storedBlock, err) = await store.GetBlock(index);
            Assert.Null(err);

            var ok = storedBlock.Signatures.TryGetValue(participants[0].Hex, out var val1Sig);
            Assert.True(ok, "Validator1 signature not stored in block");

            Assert.Equal(sig1.Signature, val1Sig);

            ok = storedBlock.Signatures.TryGetValue(participants[1].Hex, out var val2Sig);
            Assert.True(ok, "Validator2 signature not stored in block");

            Assert.Equal(sig2.Signature, val2Sig);
        }
    }
}