using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Babble.Core.Crypto;
using Babble.Core.HashgraphImpl.Model;
using Babble.Core.Util;
using Serilog;

namespace Babble.Core.ProxyImpl.InMem
{
    public class InMemProxy : IAppProxy
    {
        private  BufferBlock<byte[]> submitCh;
    
        private  ILogger logger;

        public IProxyHandler handler;

        public static InMemProxy NewInMemProxy(IProxyHandler handler, ILogger logger)
        {
            return new InMemProxy()
            {
                handler=handler,
                submitCh = new BufferBlock<byte[]>(),
                logger=logger
            };
        }
        
        //-------------------------------------------------------
        //Implement AppProxy Interface

        public virtual Task SubmitTx(byte[] tx)
        {
            return submitCh.SendAsync(tx.ToArray());
        }
    
        /*******************************************************************************
        * Implement AppProxy Interface                                                 *
        *******************************************************************************/

        //SubmitCh returns the channel of raw transactions

        public BufferBlock<byte[]> SubmitCh()
        {
            return submitCh;
        }

        //CommitBlock calls the commitHandler
        public async Task<(byte[] stateHash, ProxyError err)> CommitBlock(Block block)
        {
            var (stateHash, err) = await handler.CommitHandler(block);
            logger.Debug("InmemProxy.CommitBlock RoundReceived={RoundReceived}; Txs={Txs}; StateHash={StateHash}; Err={Err}", block.RoundReceived(), block.Transactions().Length, stateHash, err);
            return (stateHash, err);
        }

        //GetSnapshot calls the snapshotHandler
        public async Task<(byte[], ProxyError err)> GetSnapshot(int blockIndex)
        {
            var (snapshot, err) = await handler.SnapshotHandler(blockIndex);
            logger.Debug("InmemProxy.GetSnapshot Block={BlockIndex}; Snapshot={Snapshot}; Err={Err}", blockIndex, snapshot, err);
            return (snapshot, err);
        }

        //Restore calls the restoreHandler
        public async Task<ProxyError> Restore(byte[] snapshot)
        {
            var (stateHash, err) = await handler.RestoreHandler(snapshot);
            logger.Debug("InmemProxy.Restore StateHash={StateHash}; Err={Err}", stateHash, err);
            return err;

        }

    }
}