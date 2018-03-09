using Babble.Core.HashgraphImpl.Model;
using Nito.AsyncEx;

namespace Babble.Core.ProxyImpl
{
    public interface IBabbleProxy
    {
        AsyncProducerConsumerQueue<Block> CommitCh();
        ProxyError SubmitTx(byte[] tx);
    }
}