using System.IO;
using System.Threading.Tasks;
using Dotnatter.Util;
using Nito.AsyncEx;

namespace Dotnatter.NetImpl
{
    public class Rpc
    {
        public object Command { get; set; }
        public AsyncProducerConsumerQueue<RpcResponse> RespChan { get; set; }

        public async Task RespondAsync(object resp , NetError err)
        {
          await  RespChan.EnqueueAsync(new RpcResponse() {Response = resp, Error = err});
        }
    }
}