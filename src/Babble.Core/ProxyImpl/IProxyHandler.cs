using System.Threading.Tasks;
using Babble.Core.HashgraphImpl.Model;

namespace Babble.Core.ProxyImpl
{
/*
These types are exported and need to be implemented and used by the calling
application.
*/

    public interface IProxyHandler
    {
        //CommitHandler is called when Babble commits a block to the application. It
        //returns the state hash resulting from applying the block's transactions to the
        //state
        Task<(byte[] stateHash, ProxyError err)> CommitHandler(Block block);

        //SnapshotHandler is called by Babble to retrieve a snapshot corresponding to a
        //particular block
        Task<(byte[] stateHash, ProxyError err)> SnapshotHandler(int blockIndex);

        //RestoreHandler is called by Babble to restore the application to a specific
        //state
        Task<(byte[] stateHash, ProxyError err)> RestoreHandler(byte[] snapshot);
    }
}