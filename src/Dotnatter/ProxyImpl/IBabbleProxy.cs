using Dotnatter.NodeImpl;
using Nito.AsyncEx;

namespace Dotnatter.ProxyImpl
{
    public interface IBabbleProxy
    {
        AsyncProducerConsumerQueue<byte[]> CommitCh();
        ProxyError SubmitTx(byte[] tx);
    }
}