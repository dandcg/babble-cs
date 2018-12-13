using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Babble.Core.HashgraphImpl.Model;
using Nito.AsyncEx;

namespace Babble.Core.ProxyImpl
{

public interface IAppProxy
{
    BufferBlock<byte[]> SubmitCh();
    Task<(byte[] stateHash, ProxyError err)> CommitBlock(Block tx);
}
}
