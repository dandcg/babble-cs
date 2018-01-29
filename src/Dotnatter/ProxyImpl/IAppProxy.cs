using Dotnatter.NodeImpl;
using Nito.AsyncEx;

namespace Dotnatter.ProxyImpl
{

public interface IAppProxy
{
    AsyncProducerConsumerQueue<byte[]> SubmitCh();
    ProxyError CommitTx(byte[] tx);
}
}
