using Babble.Core.HashgraphImpl.Model;
using Nito.AsyncEx;

namespace Babble.Core.ProxyImpl
{

public interface IAppProxy
{
    AsyncProducerConsumerQueue<byte[]> SubmitCh();
    (byte[] stateHash, ProxyError err) CommitBlock(Block tx);
}
}
