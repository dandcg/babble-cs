using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Nito.AsyncEx;

namespace Babble.Core.NetImpl
{
    public class Rpc
    {
        public object Command { get; set; }
        public BufferBlock<RpcResponse> RespChan { get; set; }

        public Task RespondAsync(object resp , NetError err)
        {
            return RespChan.SendAsync(new RpcResponse() { Response = resp, Error = err });
        }
    }
}