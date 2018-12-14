using System.Threading.Tasks;
using Babble.Core.ProxyImpl.InMem;
using Serilog;

namespace Babble.Core.ProxyImpl.Dummy
{
    public class InMemDummy : InMemProxy, IAppProxy
    {
        private InMemProxy inmemProxy;
        private State state;
        private ILogger logger;


        public static InMemDummy NewInMemAppDummyClient(ILogger logger)
        {
            var state = State.NewState(logger);

            var proxy = InMemProxy.NewInMemProxy(state, logger);

            var client = new InMemDummy
            {
                inmemProxy = proxy, 
                state = state, 
                logger = logger
            };

            return client;
        }

        //SubmitTx sends a transaction to the Babble node via the InmemProxy

        public override Task SubmitTx(byte[] tx)
        {
            return inmemProxy.SubmitTx(tx);
        }

        //GetCommittedTransactions returns the state's list of transactions
        public byte[][] GetCommittedTransactions()
        {
            return state.GetCommittedTransactions().ToArray();
        }
    }
}