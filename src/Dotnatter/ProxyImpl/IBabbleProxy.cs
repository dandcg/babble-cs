using Dotnatter.HashgraphImpl;
using Dotnatter.NodeImpl;
using Dotnatter.Util;
using Nito.AsyncEx;

namespace Dotnatter.ProxyImpl
{
    public interface IBabbleProxy
    {
        AsyncProducerConsumerQueue<byte[]> CommitCh();
        ProxyError SubmitTx(byte[] tx);
    }
}