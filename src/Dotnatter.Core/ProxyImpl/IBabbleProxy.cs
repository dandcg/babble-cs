using Dotnatter.HashgraphImpl.Model;
using Dotnatter.NodeImpl;
using Nito.AsyncEx;

namespace Dotnatter.ProxyImpl
{
    public interface IBabbleProxy
    {
        AsyncProducerConsumerQueue<Block> CommitCh();
        ProxyError SubmitTx(byte[] tx);
    }
}