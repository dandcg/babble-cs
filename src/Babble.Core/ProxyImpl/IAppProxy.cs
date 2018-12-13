using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Babble.Core.HashgraphImpl.Model;

namespace Babble.Core.ProxyImpl
{
    public interface IAppProxy
    {
        BufferBlock<byte[]> SubmitCh();
        Task<(byte[] stateHash, ProxyError err)> CommitBlock(Block tx);

        Task<(byte[], ProxyError err)> GetSnapshot(int blockIndex);
        Task<ProxyError> Restore(byte[] snapshot);
    }
}