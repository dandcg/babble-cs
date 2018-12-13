using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Babble.Core.Crypto;
using Babble.Core.HashgraphImpl.Model;
using Babble.Core.Util;
using Nito.AsyncEx;
using Serilog;

namespace Babble.Core.ProxyImpl
{
    public class InMemAppProxy : IAppProxy
    {
        private readonly BufferBlock<byte[]> submitCh;
        private readonly List<byte[]> committedTransactions;
        private readonly ILogger logger;
        private byte[] stateHash;

        public InMemAppProxy(int id, ILogger logger)
        {
            submitCh = new BufferBlock<byte[]>();
            committedTransactions = new List<byte[]>();
            this.logger = logger.AddNamedContext(nameof(InMemAppProxy) ,id.ToString());
        }

        public BufferBlock<byte[]> SubmitCh()
        {
            return submitCh;
        }

        private (byte[] stateHash, ProxyError err) Commit(Block block)

        {
            committedTransactions.AddRange(block.Transactions());

            var hash = stateHash.ToArray();
            foreach (var t in block.Transactions())
            {
                var tHash = CryptoUtils.Sha256(t);
                hash = Hash.SimpleHashFromTwoHashes(hash, tHash);
            }

            stateHash = hash;

            return (stateHash, null);
        }

        public Task<(byte[] stateHash, ProxyError err)> CommitBlock(Block block)
        {
            logger.Debug("CommitBlock RoundReceived={RoundReceived}; TxCount={TxCount}", block.RoundReceived());
            return Task.FromResult(Commit(block));
        }

//-------------------------------------------------------
//Implement AppProxy Interface

        public async Task SubmitTx(byte[] tx)
        {
            logger.Debug("SubmitTx -> {TxString}", tx.BytesToString());
            await submitCh.SendAsync(tx);
            }

        public byte[][] GetCommittedTransactions()
        {
            return committedTransactions.ToArray();
        }
    }
}