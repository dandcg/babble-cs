using Babble.Core.HashgraphImpl.Model;
using Nito.AsyncEx;

namespace Babble.Core.ProxyImpl
{
    public interface IBabbleProxy
    {
        BufferBlock<Block> CommitCh();
        ProxyError SubmitTx(byte[] tx);
    }
}