using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Dotnatter.Common;
using Dotnatter.HashgraphImpl;
using Dotnatter.Test.Helpers;
using Serilog;
using Xunit;
using Xunit.Abstractions;

namespace Dotnatter.Test.HashgraphImpl
{
    public class CachesTests
    {
        private readonly ILogger logger;

        public CachesTests(ITestOutputHelper output)
        {
            logger = output.SetupLogging();
        }
 

        [Fact]
        public void TestParticipantEventsCache()
        {
            var size = 10;

            var testSize = 25;

            var participants = new Dictionary<string, int>
            {
                {"alice", 0},
                {"bob", 1},
                {"charlie", 2}
            };

            var pec = new ParticipantEventsCache(size, participants,logger);

            var items = new Dictionary<string, List<string>>();

            foreach (var p in participants)
            {
                items[p.Key] = new List<string>();
            }

            for (var i = 0; i < testSize; i++)
            {
                foreach (var p in participants)
                {
                    var item = $"{p.Key}:{i}";

                    pec.Set(p.Key, item, i);

                    var pitems = items[p.Key];

                    pitems.Add(item);

                    items[p.Key] = pitems;
                }
            }

            // GET ITEM ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

            foreach (var p in participants)
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

            foreach (var p in participants)
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
        public void TestParticipantEventsCacheEdge()
        {
            var size = 10;

            var testSize = 11;

            var participants = new Dictionary<string, int>
            {
                {"alice", 0},
                {"bob", 1},
                {"charlie", 2}
            };

            var pec = new ParticipantEventsCache(size, participants,logger);

            var items = new Dictionary<string, List<string>>();
            
            foreach (var p in participants)
            {
                items.Add(p.Key, new List<string>());
            }

            for (var i = 0; i < testSize; i++)
            {
                foreach (var p in participants)
                {
                    var item = $"{p.Key}:{i}";

                    pec.Set(p.Key, item, i);

                    var pitems = items[p.Key];

                    pitems.Add(item);
                }
            }

            foreach (var p in participants)
            {
                var expected = items[p.Key].Skip(size).ToArray();

                var (cached,err) = pec.Get(p.Key, size - 1);

                Assert.Null(err);

                cached.ShouldCompareTo(expected);
            }
        }
    }
}