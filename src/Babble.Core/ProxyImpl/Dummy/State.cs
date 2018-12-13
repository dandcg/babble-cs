using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Babble.Core.HashgraphImpl.Model;
using Serilog;

namespace Babble.Core.ProxyImpl.Dummy
{
    public class State:IProxyHandler
    {
        /*
        * The dummy App is used for testing and as an example for building Babble
        * applications. Here, we define the dummy's state which doesn't really do
        * anything useful. It saves and logs block transactions. The state hash is
        * computed by cumulatively hashing transactions together as they come in.
        * Snapshots correspond to the state hash resulting from executing a the block's
        * transactions.
         */

        private List<byte[]> committedTxs;
        private byte[] stateHash;
        private Dictionary<int, byte[]> snapshots;
        private ILogger logger;


        public static State NewState(ILogger logger)
        {
            var state = new State()
            {
                committedTxs = new List<byte[]>(),
                stateHash = new byte[] { },
                snapshots = new Dictionary<int, byte[]>(),
                logger = logger



            };


                
               
            logger.Information("Init Dummy State");

            return state;

            }


    public  async Task<(byte[] stateHash, ProxyError err)> CommitHandler(Block block)

    {
            logger.Debug("CommitBlock {Block}={Block}", block );

        var err = await commit(block);

            if (err != null)
            {
                return (null, err);
            }

        return (stateHash, null);
    }

      public  Task<(byte[] stateHash, ProxyError err)> SnapshotHandler(int blockIndex)
        
        {
            logger.Debug("GetSnapshot {BlockIndex}={BlockIndex}", blockIndex );
        
            var ok = snapshots.TryGetValue(blockIndex, out var snapshot);

            if (!ok)
            {
                return Task.FromResult<(byte[], ProxyError)>((null, new ProxyError($"Snapshot {blockIndex} not found")));
            }

            return Task.FromResult<(byte[], ProxyError)>((snapshot, null));
        }

        public Task<(byte[] stateHash, ProxyError err)> RestoreHandler(byte[] snapshot)
        {
            //XXX do something smart here
            stateHash = snapshot;

            return Task.FromResult<(byte[], ProxyError)>((stateHash, null));
        }

        public List<byte[]> GetCommittedTransactions()
        {
            return committedTxs;
        }

      public async Task<ProxyError> commit(Block block)
      {
          committedTxs.AddRange(block.Transactions());

            //log tx and update state hash
          var hash = stateHash;

            foreach (var tx in block.Transactions())
            {
                logger.Information(tx.ToString());

                hash = Crypto.Hash.SimpleHashFromTwoHashes(hash, Crypto.Hash.Sha256(tx));
            }

          stateHash = hash;

            //XXX do something smart here
          snapshots[block.Index()] = hash;

          return null;
      }
    }
}
