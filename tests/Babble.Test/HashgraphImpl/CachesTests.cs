using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Babble.Core.Common;
using Babble.Core.HashgraphImpl;
using Babble.Core.PeersImpl;
using Babble.Core.Util;
using Babble.Test.Utils;
using Serilog;
using Xunit;
using Xunit.Abstractions;

namespace Babble.Test.HashgraphImpl
{
    public class CachesTests
    {
        private readonly ILogger logger;
        private readonly ITestOutputHelper output;

        public CachesTests(ITestOutputHelper output)
        {
            logger = output.SetupLogging();
            this.output = output;
        }





        [Fact]
        public async Task TestParticipantEventsCache()
        {
            var size = 10;

            var testSize = 25;

            var participants =Peers.NewPeersFromSlice(
            new []
            {

                Peer.New("0xaa", ""),
                Peer.New("0xbb", ""),
                Peer.New("0xcc", ""),

            });
                
            var pec = await ParticipantEventsCache.NewParticipantEventsCache(size, participants);

            var items = new Dictionary<string, List<string>>();

            foreach (var p in participants.ByPubKey)
            {
                items[p.Key] = new List<string>();
            }

            for (var i = 0; i < testSize; i++)
            {
                foreach (var p in participants.ByPubKey)
                {
                    var item = $"{p.Key}:{i}";

                    pec.Set(p.Key, item, i);

                    var pitems = items[p.Key];

                    pitems.Add(item);

                    items[p.Key] = pitems;
                }
            }

            // GET ITEM ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

            foreach (var p in participants.ByPubKey)
            {
                var index1 = 9;

                var (_,err) =  pec.GetItem(p.Key, index1);
                Assert.Equal(StoreErrorType.TooLate, err.StoreErrorType);

                //--

                var index2 = 15;

                var expected2 = items[p.Key][index2];

                var (actual2,err2) = pec.GetItem(p.Key, index2);

                Assert.Null(err2);

                actual2.ShouldCompareTo(expected2);

                //--

                var index3 = 27;

                var expected3 = new string[] { };
                var (actual3,err3) = pec.Get(p.Key, index3);

                actual3.ShouldCompareTo(expected3);
            }

            // KNOWN ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

            var known = pec.Known();

            foreach (var (p, k) in known)
            {
                var expectedLastIndex = testSize - 1;

                if (k != expectedLastIndex)
                {
                    throw new Exception(string.Format("KnownEvents[{0}] should be {1}, not {2}", p, expectedLastIndex, k));
                }
            }

            // GET ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

            foreach (var p in participants.ByPubKey)
            {
                var (_,err) =  pec.Get(p.Key, 0);
                Assert.Equal(StoreErrorType.TooLate, err.StoreErrorType);

                //--

                var skipIndex = 9;

                var expected = items[p.Key].Skip(skipIndex + 1).ToArray();

                var (cached, errc1) = pec.Get(p.Key, skipIndex);

                Assert.Null(errc1);

                cached.ShouldCompareTo(expected);

                //--

                var skipIndex2 = 15;

                var expected2 = items[p.Key].Skip(skipIndex2 + 1).ToArray();

             var (cached2,errc2) = pec.Get(p.Key, skipIndex2);

                Assert.Null(errc2);

                cached2.ShouldCompareTo(expected2);

                //--

                var skipIndex3 = 27;

                var expected3 = new string[] { };

                var (cached3, errc3) = pec.Get(p.Key, skipIndex3);

                Assert.Null(errc3);

                cached3.ShouldCompareTo(expected3);
            }
        }

        [Fact]
        public async Task TestParticipantEventsCacheEdge()
        {
            var size = 10;

            var testSize = 11;

            var participants =Peers.NewPeersFromSlice(
                new []
                {

                    Peer.New("0xaa", ""),
                    Peer.New("0xbb", ""),
                    Peer.New("0xcc", ""),

                });

            var pec =await ParticipantEventsCache.NewParticipantEventsCache(size, participants);

            var items = new Dictionary<string, List<string>>();
            
            foreach (var p in participants.ByPubKey)
            {
                items.Add(p.Key, new List<string>());
            }

            for (var i = 0; i < testSize; i++)
            {
                foreach (var p in participants.ByPubKey)
                {
                    var item = $"{p.Key}:{i}";

                    pec.Set(p.Key, item, i);

                    var pitems = items[p.Key];

                    pitems.Add(item);
                }
            }

            foreach (var p in participants.ByPubKey)
            {
                var expected = items[p.Key].Skip(size).ToArray();

                var (cached,err) = pec.Get(p.Key, size - 1);

                Assert.Null(err);

                cached.ShouldCompareTo(expected);
            }
        }
    }
}