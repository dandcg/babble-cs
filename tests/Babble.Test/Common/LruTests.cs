using System;
using Babble.Core.Common;
using Dotnatter.Test.Helpers;
using Serilog;
using Xunit;
using Xunit.Abstractions;

namespace Dotnatter.Test.Common
{
    public class LruTests
    {

        private readonly ILogger logger;

        public LruTests(ITestOutputHelper output)
        {
            logger = output.SetupLogging();
        }

        [Fact]
        public void TestLru()
        {
            // ReSharper disable once NotAccessedVariable
            var evictCounter = 0;

            var onEvicted = new Action<int, int>((k, v) =>
            {
                Assert.Equal(k, v);
                evictCounter += 1;
            });

            var l = new LruCache<int, int>(128, onEvicted, logger);

            var i = 0;
            for (i = 0; i < 256; i++)
            {
                l.Add(i, i);
            }

            // Evict counter
            Assert.Equal(128, l.Len());

            i = 0;
            foreach (var k in l.Keys())
            {
                var (v,_) = l.Get(k);

                Assert.Equal(v, k);
                Assert.Equal(v, i + 128);


                i++;
            }
            
            for ( i = 0; i < 128; i++)
            {
                var (_, res) =l.Get(i);
                Assert.False(res, "should be evicted");

            }

            for (i = 128; i < 256; i++)
            {
                var (_, res) = l.Get(i);
                Assert.True(res, "should not be evicted");

            }
            
            for (i = 128; i < 192; i++)
            {
                var res = l.Remove(i);
                Assert.True(res, "should be contained");

              res = l.Remove(i);
                Assert.False(res, "should not be contained");

                (_,res) = l.Get(i);
                Assert.False(res, "should be deleted");
            }

            l.Get(192); // expect 192 to be last key in l.Keys()

            i = 0;
            foreach (var k in l.Keys())
            {

                Assert.True((i < 63 && k != i + 193) || (i == 63 && k != 192), $"out of order key: {k}");
                i++;

            }

            l.Purge();

            Assert.Equal(0, l.Len());

            var ( _ , r)= l.Get(200);
            Assert.False(r, "should contain nothing");
        }


        // Test that Set returns true/false if an eviction occurred
        [Fact]
        public void TestLru_Add()
        {
            var evictCounter = 0;

            var onEvicted = new Action<int, int>((k, v) =>
            {
                Assert.Equal(k, v);
                evictCounter += 1;
            });

            var l = new LruCache<int,int>(1, onEvicted, logger);
            
            //should not have an eviction
            Assert.False(l.Add(1, 1));
            Assert.Equal(0,evictCounter);

            //should have an eviction
            Assert.True(l.Add(2, 2));
            Assert.Equal(1,evictCounter);

        }

        // Test that Contains doesn't update recent-ness
        [Fact]
        public void TestLru_Contains()
        {
            var l = new LruCache<int, int>(2, null, logger);
            
            l.Add(1, 1);

            l.Add(2, 2);

            Assert.True(l.Contains(1), "1 should be contained");

            l.Add(3, 3);

            Assert.False(l.Contains(1), "Contains should not have updated recent-ness of 1");

        }

        // Test that Peek doesn't update recent-ness
        [Fact]
        public void TestLru_Peek()
        {
            var l = new LruCache<int, int>(2, null, logger);

            l.Add(1, 1);

            l.Add(2, 2);

            var (v,res)=l.Peek(1);

            Assert.True(res);
            Assert.Equal(1,v);
      
            l.Add(3, 3);

            Assert.False(l.Contains(1), "should not have updated recent-ness of 1");

        }
     
    }

}