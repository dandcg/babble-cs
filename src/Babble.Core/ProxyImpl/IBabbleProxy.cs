using Dotnatter.Core.HashgraphImpl.Model;
using Nito.AsyncEx;

namespace Dotnatter.Core.ProxyImpl
{
    public interface IBabbleProxy
    {
        AsyncProducerConsumerQueue<Block> CommitCh();
        ProxyError SubmitTx(byte[] tx);
    }
}