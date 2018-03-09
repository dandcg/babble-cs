namespace Babble.Core.NetImpl
{
    // RPCResponse captures both a response and a potential error.
    public class RpcResponse
    {
        public object Response { get; set; }
        public NetError Error { get; set; }
    }

// RPC has a command, and provides a response mechanism.

    // Transport provides an interface for network transports
// to allow a node to communicate with other nodes.

    // WithPeers is an interface that a transport may provide which allows for connection and
// disconnection.
// "Connect" is likely to be nil.

// LoopbackTransport is an interface that provides a loopback transport suitable for testing
// e.g. InmemTransport. It's there so we don't have to rewrite tests.
}