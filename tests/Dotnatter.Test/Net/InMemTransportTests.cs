using System;
using System.Collections.Generic;
using System.Text;
using Dotnatter.NetImpl;
using Dotnatter.NetImpl.TransportImpl;
using Xunit;

namespace Dotnatter.Test.Net
{
    public class InMemTransportTests
    {
        [Fact]
        public void TestInmemTransportImpl()
        {
            var t= new InMemTransport("test");
            Assert.True(t is ITransport);
            Assert.True(t is ILoopbackTransport);
            Assert.True(t is IWithPeers);
        }
    }
}
