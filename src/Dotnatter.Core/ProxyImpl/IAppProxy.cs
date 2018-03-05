using Dotnatter.HashgraphImpl.Model;
using Dotnatter.NodeImpl;
using Nito.AsyncEx;

namespace Dotnatter.ProxyImpl
{

public interface IAppProxy
{
    AsyncProducerConsumerQueue<byte[]> SubmitCh();
    (byte[] stateHash, ProxyError err) CommitBlock(Block tx);
}
}
