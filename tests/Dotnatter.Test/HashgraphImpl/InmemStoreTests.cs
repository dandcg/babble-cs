using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Dotnatter.Crypto;
using Dotnatter.HashgraphImpl;
using Dotnatter.HashgraphImpl.Model;
using Dotnatter.HashgraphImpl.Stores;
using Dotnatter.Test.Helpers;
using Dotnatter.Util;
using Serilog;
using Xunit;
using Xunit.Abstractions;

namespace Dotnatter.Test.HashgraphImpl
{
    public class InmemStoreTests
    {
        private ILogger logger;

        public InmemStoreTests(ITestOutputHelper output)
        {
             logger = output.SetupLogging();
        }

        public class Pub
        {
            public int Id { get; set; }
            public CngKey PrivKey { get; set; }
            public byte[] PubKey { get; set; }
            public string Hex { get; set; }
        }

        public (InmemStore, Pub[]) InitInmemStore(int cacheSize)
        {
            var n = 3;
            var participantPubs = new List<Pub>();
            var participants = new Dictionary<string, int>();
            for (var i = 0; i < n; i++)
            {
                var key = CryptoUtils.GenerateEcdsaKey();
                var pubKey = CryptoUtils.FromEcdsaPub(key);
                var hex = pubKey.ToHex();
                participantPubs.Add(new Pub {Id = i, PrivKey = key, PubKey = pubKey, Hex = hex});
                participants.Add(hex, i);
            }

            var store = new InmemStore(participants, cacheSize, Log.Logger);
            return (store, participantPubs.ToArray());
        }

        [Fact]
        public void TestInmemEvents()
        {
            var cacheSize = 100;

            var testSize = 15;

            var (store, participants) = InitInmemStore(cacheSize);

            var events = new Dictionary<string, List<Event>>();

            foreach (var p in participants)
            {
                var items = new List<Event>();

                for (var k = 0; k < testSize; k++)
                {
                    var ev = new Event(new[] {$"{p.Hex}_{k}".StringToBytes()},
                        new[] {"", ""},
                        p.PubKey,
                        k);

                    ev.Hex(); //just to set private variables
                    items.Add(ev);
                    store.SetEvent(ev);
                }

                events[p.Hex] = items;
            }

            foreach (var evi in events)
            {
                foreach (var ev in evi.Value)
                {
                    var (rev, err) = store.GetEvent(ev.Hex());

                    Assert.Null(err);
                    rev.Body.ShouldCompareTo(ev.Body);
                }
            }

            var skipIndex = -1; //do not skip any indexes
            foreach (var p in participants)
            {
                var (pEvents, err) = store.ParticipantEvents(p.Hex, skipIndex);

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

            var expectedKnown = new Dictionary<int, int>();

            foreach (var p in participants)
            {
                expectedKnown.Add(p.Id, testSize - 1);
            }

            var known = store.Known();

            known.ShouldCompareTo(expectedKnown);

            foreach (var p in participants)
            {
                var evs = events[p.Hex];
                foreach (var ev in evs)

                {
                    store.AddConsensusEvent(ev.Hex());
                }
            }
        }

        [Fact]
        public void TestInmemRounds()
        {
            var ( store, participants) = InitInmemStore(10);

            var round = new RoundInfo();

            var events = new Dictionary<string, Event>();

            foreach (var p in participants)
            {
                var ev = new Event(new[] {new byte[] { }},
                    new[] {"", ""},
                    p.PubKey,
                    0);
                events[p.Hex] = ev;
                round.AddEvent(ev.Hex(), true);
            }

            store.SetRound(0, round);

            var c = store.LastRound();
            Assert.Equal(0, c);

            var (storedRound, err) = store.GetRound(0);
            Assert.Null(err);

            round.ShouldCompareTo(storedRound);

            var witnesses = store.RoundWitnesses(0);
            var expectedWitnesses = round.Witnesses();

            Assert.Equal(expectedWitnesses.Length, witnesses.Length);

            foreach (var w in expectedWitnesses)
            {
                Assert.Contains(w, witnesses);
            }
        }
    }
}