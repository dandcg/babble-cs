using Babble.Core.NetImpl.TransportImpl;
using Xunit;

namespace Babble.Test.Net
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
