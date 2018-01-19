using System.IO;
using Dotnatter.Util;

namespace Dotnatter.NetImpl
{
    public class Rpc
    {
        public object Command { get; set; }
        public Stream Reader { get; set; }
        public Channel<RpcResponse> RespChan { get; set; }
    }
}