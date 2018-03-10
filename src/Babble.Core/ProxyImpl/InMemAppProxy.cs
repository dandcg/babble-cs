using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Babble.Core.Crypto;
using Babble.Core.HashgraphImpl.Model;
using Nito.AsyncEx;
using Serilog;

namespace Babble.Core.ProxyImpl
{
    public class InMemAppProxy : IAppProxy
    {
        private readonly AsyncProducerConsumerQueue<byte[]> submitCh;
        private readonly List<byte[]> committedTransactions;
        private readonly ILogger logger;
        private byte[] stateHash;

        public InMemAppProxy(ILogger logger)
        {
            submitCh = new AsyncProducerConsumerQueue<byte[]>();
            committedTransactions = new List<byte[]>();
            this.logger = logger;
        }

        public AsyncProducerConsumerQueue<byte[]> SubmitCh()
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
            logger.Debug("InmemProxy CommitBlock RoundReceived={RoundReceived}; TxCount={TxCount}", block.RoundReceived());
            return Task.FromResult(Commit(block));
        }

//-------------------------------------------------------
//Implement AppProxy Interface

        public Task SubmitTx(byte[] tx)
        {
            return submitCh.EnqueueAsync(tx);
        }

        public byte[][] GetCommittedTransactions()
        {
            return committedTransactions.ToArray();
        }
    }
}