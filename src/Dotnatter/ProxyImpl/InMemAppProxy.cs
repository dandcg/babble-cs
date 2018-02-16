using System.Collections.Generic;
using System.Threading.Tasks;
using Nito.AsyncEx;
using Serilog;

namespace Dotnatter.ProxyImpl
{
    public class InMemAppProxy : IAppProxy
    {
        private readonly AsyncProducerConsumerQueue<byte[]> submitCh;
        private readonly List<byte[]> commitedTxs;
        private readonly ILogger logger;

        public InMemAppProxy(ILogger logger)
        {
            submitCh = new AsyncProducerConsumerQueue<byte[]>();
            commitedTxs = new List<byte[]>();

            this.logger = logger;
        }

        public AsyncProducerConsumerQueue<byte[]> SubmitCh()
        {
            return submitCh;
        }

        public ProxyError CommitTx(byte[] tx)
        {
            logger.ForContext("tx", tx).Debug("InmemProxy CommitTx");
            commitedTxs.Add(tx);
            return null;
        }

//-------------------------------------------------------
//Implement AppProxy Interface

        public async Task SubmitTx(byte[] tx)
        {
            await submitCh.EnqueueAsync(tx);
        }

        public byte[][] GetCommittedTransactions()
        {
            return commitedTxs.ToArray();
        }
    }
}