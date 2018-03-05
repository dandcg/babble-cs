using Dotnatter.Core.HashgraphImpl.Model;
using Nito.AsyncEx;

namespace Dotnatter.Core.ProxyImpl
{

public interface IAppProxy
{
    AsyncProducerConsumerQueue<byte[]> SubmitCh();
    (byte[] stateHash, ProxyError err) CommitBlock(Block tx);
}
}
