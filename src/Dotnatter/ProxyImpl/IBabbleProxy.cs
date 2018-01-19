using Dotnatter.HashgraphImpl;
using Dotnatter.NodeImpl;
using Dotnatter.Util;

namespace Dotnatter.ProxyImpl
{
    public interface IBabbleProxy
    {
        Channel<byte[]> CommitCh();
        ProxyError SubmitTx(byte[] tx);
    }
}