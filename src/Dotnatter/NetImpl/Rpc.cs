using System.IO;
using Dotnatter.Util;
using Nito.AsyncEx;

namespace Dotnatter.NetImpl
{
    public class Rpc
    {
        public object Command { get; set; }
        public AsyncProducerConsumerQueue<RpcResponse> RespChan { get; set; }
    }
}